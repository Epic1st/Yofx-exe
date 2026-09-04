---
agent_id: G03
lane: Process Isolation & Launch Boundary
scope:
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContractValidator.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContracts.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerDeploymentPathPolicy.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerLaunchManifest.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedBrokerConnectionProbeClient.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedBrokerProcessOptions.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs
status: COMPLETE
generated: 2026-08-29T11:29:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G03 — Process Isolation & Launch Boundary

## Scope audited
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContractValidator.cs` (438 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContracts.cs` (127 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerDeploymentPathPolicy.cs` (137 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerLaunchManifest.cs` (341 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedBrokerConnectionProbeClient.cs` (123 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedBrokerProcessOptions.cs` (150 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs` (227 lines)

## Verdict
Sound. The broker worker process boundary enforces defense-in-depth against command-line injection, path traversal, binary plant/hijacking, and credential leaks. Executable and manifest paths are strictly constrained to dedicated fixed-volume directories with reparse-point ancestry validation, exhaustive closure hashing with persistent read pins (`FileShare.Read`), zero command-line arguments, fully cleared/whitelisted environment variables, and strict bi-directional HMAC/JSON contract validation.

## Findings
None. The audited area holds up against all focus attack vectors: executable path resolution prevents symlink/device/network escaping, arguments are not used (all IPC is over length-delimited HMAC-SHA256 authenticated streams on stdin/stdout), credentials are never passed in plaintext, process environment is cleared and whitelisted, and child processes are bound by absolute wall-clock deadlines.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs:93` — Parameter order validation (`ArgumentOutOfRangeException.ThrowIfGreaterThan(fromUtc, toUtc)`) in `GetDealsAsync` is not exercised by test suites.
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContractValidator.cs:288-291` — Maximum collection boundary rejections (`Positions.Count > 2048`, `Orders.Count > 2048`, `Deals.Count > 2048`) in `ValidateSnapshot` are not covered by unit tests.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 82.7s | 196667 tok | id=cbaee8bf-b263-4234-bd80-f1d0a7235158
