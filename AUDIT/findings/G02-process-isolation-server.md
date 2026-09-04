---
agent_id: G02
lane: Process Isolation IPC Protocol & Worker Servers
scope:
  - src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerWorkerServer.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerConnectionProbeWorkerServer.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G02 — Process Isolation IPC Protocol & Worker Servers

## Scope audited
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerWorkerServer.cs` (135 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerConnectionProbeWorkerServer.cs` (95 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs` (302 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)

## Verdict
The IPC protocol, framing, worker server dispatchers, and process boundary client are exceptionally sound and fail closed across all execution paths. Peer authentication over anonymous standard I/O pipes is enforced before payload deserialization using direction-tagged HMAC-SHA256 with constant-time equality checks and fresh per-run 256-bit session keys. Framing lengths are strictly bounded against memory exhaustion, deserialization is strictly typed with unmapped members disallowed, child process environments are fully scrubbed, and process tree termination is tracked without false completion claims.

## Findings
None.

The audited area demonstrates rigorous defense-in-depth across the trust boundary:
1. **Transport Isolation & Network Exposure:** The transport exclusively utilizes anonymous OS pipes created via `ProcessStartInfo` (`RedirectStandardInput`, `RedirectStandardOutput`, `RedirectStandardError`). There are no TCP/UDP listeners or network-accessible named pipes. Child environment variables are completely cleared before launch to prevent ambient secret inheritance, and .NET diagnostic ports are explicitly disabled (`DOTNET_EnableDiagnostics=0`).
2. **Peer Authentication & Constant-Time Validation:** A cryptographically secure 256-bit random session key is generated per invocation. The initial handshake requires an exact bootstrap magic match (`YO4XIPC1`), verified via `CryptographicOperations.FixedTimeEquals`. Every subsequent frame computes an HMAC-SHA256 tag over distinct direction strings (`yo4x-broker-request-v1` vs `yo4x-broker-response-v1`), length, and payload. The tag is validated via `CryptographicOperations.FixedTimeEquals` before any payload deserialization or execution occurs.
3. **Protocol Framing & Resource Bounds:** All frame lengths are 4-byte big-endian integers bounded by strict minimums and maximums (`DefaultMaximumRequestBytes = 128 KB`, `DefaultMaximumResponseBytes = 1 MB`, configured within `4 KB` to `4 MB`). Unbounded or non-positive lengths immediately throw `InvalidDataException`. Stream reads utilize `Stream.ReadExactlyAsync`, preventing partial frame desynchronization.
4. **Deserialization Safety:** JSON deserialization (`System.Text.Json`) targets sealed record types with `JsonUnmappedMemberHandling.Disallow`, `PropertyNameCaseInsensitive = false`, `NumberHandling = JsonNumberHandling.Strict`, `AllowTrailingCommas = false`, and `MaxDepth = 64`. No polymorphic type discriminators or type resolvers exist.
5. **Replay & Lifecycle Protection:** Each worker process is single-use (`RunOnceAsync`). Fresh ephemeral session keys prevent cross-session frame replay, and contracts enforce UTC deadlines bounded within 2 minutes of generation. On any anomaly, cancellation, or unauthenticated frame, `Kill(entireProcessTree: true)` is requested, payloads and key buffers are zeroed via `CryptographicOperations.ZeroMemory`, and `mt5_process_termination_unconfirmed` is returned if clean exit cannot be guaranteed.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 72.8s | 161769 tok | id=37490914-44db-4fcb-97eb-927d86394264
