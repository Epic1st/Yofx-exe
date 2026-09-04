---
agent_id: G09
lane: mt5-workerhost
scope:
  - src/Runtime/YO4X.Mt5.WorkerHost/Program.cs
  - src/Runtime/YO4X.Mt5.WorkerHost/YO4X.Mt5.WorkerHost.csproj
status: COMPLETE
generated: 2026-08-29T11:28:35Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G09 — mt5-workerhost

## Scope audited
- `src/Runtime/YO4X.Mt5.WorkerHost/Program.cs` (10 lines)
- `src/Runtime/YO4X.Mt5.WorkerHost/YO4X.Mt5.WorkerHost.csproj` (19 lines)

## Verdict
The MT5 worker host entry point is sound, minimal, and strictly fail-closed. It wires `AuthenticatedBrokerWorkerServer` with `Mt5ProofOnlyBrokerWorkerExecutor` and executes exactly one authenticated request cycle before terminating. The host maintains absolute process isolation: it accepts no untrusted ambient CLI or environment parameters, maintains zero resident background state across operations, disables all live trading mutations at the architectural boundary, and wipes cryptographic session material on exit.

## Findings
None. The component is intentionally minimal and robust. It instantiates `AuthenticatedBrokerWorkerServer` with `Mt5ProofOnlyBrokerWorkerExecutor`, pipes `Console.OpenStandardInput()` to `Console.OpenStandardOutput()`, and executes `RunOnceAsync`. Unhandled faults during bootstrap, decryption, schema validation, or command execution cause immediate exit with code 70 without logging sensitive material, while cryptographic keys and payloads are zeroed in memory before process exit.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mt5.WorkerHost/Program.cs:6-9` — End-to-end execution of the compiled `YO4X.Mt5.WorkerHost.exe` binary under `BrokerProcessClient` is not directly exercised in test suites (the process boundary tests target the dedicated `YO4X.BrokerProcess.TestWorker` fixture while `ArchitectureBoundaryTests` only verifies project reference constraints).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 63.9s | 144911 tok | id=010953a0-ef7e-4cf2-9780-21ab71381a09
