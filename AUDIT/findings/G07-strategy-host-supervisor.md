---
agent_id: G07
lane: Strategy Host and Supervisor Runtime
scope:
  - src/Runtime/YO4X.StrategyHost/**
  - src/Runtime/YO4X.Supervisor/**
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 2, P2: 2, P3: 1 }
---

# G07 — Strategy Host and Supervisor Runtime

## Scope audited
- `src/Runtime/YO4X.StrategyHost/Program.cs` (20 lines)
- `src/Runtime/YO4X.StrategyHost/StrategyExecutionCoordinator.cs` (27 lines)
- `src/Runtime/YO4X.StrategyHost/StrategyHostRuntimeStatus.cs` (25 lines)
- `src/Runtime/YO4X.StrategyHost/YO4X.StrategyHost.csproj` (14 lines)
- `src/Runtime/YO4X.StrategyHost/appsettings.json` (10 lines)
- `src/Runtime/YO4X.StrategyHost/appsettings.Development.json` (9 lines)
- `src/Runtime/YO4X.StrategyHost/Properties/launchSettings.json` (24 lines)
- `src/Runtime/YO4X.Supervisor/Program.cs` (21 lines)
- `src/Runtime/YO4X.Supervisor/SupervisorRuntimeStatus.cs` (25 lines)
- `src/Runtime/YO4X.Supervisor/SupervisorUserOperationProtocolRegistration.cs` (55 lines)
- `src/Runtime/YO4X.Supervisor/Properties/AssemblyInfo.cs` (4 lines)
- `src/Runtime/YO4X.Supervisor/YO4X.Supervisor.csproj` (18 lines)
- `src/Runtime/YO4X.Supervisor/appsettings.json` (13 lines)
- `src/Runtime/YO4X.Supervisor/appsettings.Development.json` (9 lines)
- `src/Runtime/YO4X.Supervisor/Properties/launchSettings.json` (24 lines)

## Verdict
The audited runtime hosts currently exist as skeleton web application shells with static HTTP health probes and fail-closed protocol gates, but lack active strategy lifecycle orchestration. `StrategyExecutionCoordinator` performs bounded validation on successful evaluation returns but fails unhandled when strategy code throws, while `YO4X.Supervisor` contains no child process supervision, restart backoff, restart limits, or orphan prevention. Health endpoints report purely binary process presence rather than the operational health or faulted status of the running strategy.

## Findings

### [P1] Uncaught exceptions in strategy execution bypass bounded validation and crash host
- **Where:** `src/Runtime/YO4X.StrategyHost/StrategyExecutionCoordinator.cs:21-25`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        long startedAt = Stopwatch.GetTimestamp();
        StrategyResult result = strategy.Handle(input, snapshot, currentState);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        return StrategyResultValidator.Validate(currentState, result, bounds, elapsed);
  ```
- **Failure:** When a user strategy throws an unhandled runtime exception (e.g., `NullReferenceException`, `DivideByZeroException`, or `IndexOutOfRangeException` during `strategy.Handle`), `Execute` contains no exception boundary. The exception propagates unhandled out of `Execute` instead of returning a `StrategyResultValidation` with `StrategyResultValidationCode.StrategyFaulted` and reason `"strategy_execution_faulted"`, crashing the host execution pipeline.
- **Fix:** Wrap the `strategy.Handle` call in a `try/catch` block, measure elapsed time on exception, and return a `StrategyResultValidation` indicating `StrategyResultValidationCode.StrategyFaulted` with a structured fault reason.

### [P1] Supervisor host lacks process supervision, restart backoff, and crash loop limits
- **Where:** `src/Runtime/YO4X.Supervisor/Program.cs:4-16`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
builder.Services.AddSingleton<SupervisorRuntimeStatus>();
builder.Services.AddSupervisorUserOperationProtocol(builder.Configuration);

var app = builder.Build();

app.MapGet("/health/live", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();
```
- **Failure:** `YO4X.Supervisor` hosts only WebApplication endpoints and registers no background worker or process management service. A failing strategy process has no supervisor managing restart backoff, exponential delays, maximum restart attempt limits, or quarantine parking (`RuntimeComponentState.Faulted`/`Stopped`), resulting in immediate uncontrolled restart loops or unmanaged crash behavior.
- **Fix:** Register a background worker service in `YO4X.Supervisor` that manages child strategy process execution, monitors exit codes, enforces exponential restart backoffs with configurable maximum attempt thresholds, and marks persistently failing strategies as permanently parked.

### [P2] StrategyHost health endpoints report healthy process liveness regardless of strategy state
- **Where:** `src/Runtime/YO4X.StrategyHost/Program.cs:8-14`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
app.MapGet("/health/live", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));
```
- **Failure:** `/health/live` and `/health/startup` return HTTP 200 OK immediately based solely on host process startup. If a strategy fails to load, faults in evaluation, or deadlocks, `/health/live` continues to return HTTP 200 OK with `"strategy_host_process_live"`, preventing orchestrators from detecting deadlocked or faulted strategy instances.
- **Fix:** Make `StrategyHostRuntimeStatus` dynamic and update `/health/live` and `/health/startup` to check actual strategy package initialization, worker health, and evaluation responsiveness.

### [P2] Supervisor health probes expose immutable status disconnected from managed worker states
- **Where:** `src/Runtime/YO4X.Supervisor/SupervisorRuntimeStatus.cs:7-24`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    public PublicRuntimeHealth Live { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "live",
        "supervisor_process_live");

    public PublicRuntimeHealth Startup { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "started",
        "supervisor_startup_complete");

    public PublicRuntimeHealth Ready { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "not-ready",
        "runtime_component_evidence_incomplete");
```
- **Failure:** All health properties (`Live`, `Startup`, `Ready`) are immutable get-only properties instantiated at startup. If child processes crash, fail lease verification, or enter fence states, `SupervisorRuntimeStatus` cannot reflect these state changes to external monitoring or orchestration layers.
- **Fix:** Refactor `SupervisorRuntimeStatus` to support thread-safe snapshot updates (similar to `GatewayHostRuntimeStatus`) driven by actual runtime evidence and lease state transitions.

### [P3] StrategyHost defines duplicate local health contract bypassing shared runtime contracts
- **Where:** `src/Runtime/YO4X.StrategyHost/StrategyHostRuntimeStatus.cs:3-11`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
public sealed record StrategyHostHealth(int ContractVersion, string Role, string Status, string Code);

public sealed class StrategyHostRuntimeStatus
{
    public StrategyHostHealth Live { get; } = new(
        1,
        "strategy-host",
        "live",
        "strategy_host_process_live");
```
- **Failure:** `StrategyHostRuntimeStatus` defines a redundant record `StrategyHostHealth` and hardcodes integer `1` rather than referencing `YO4X.Runtime.Contracts.PublicRuntimeHealth` and `RuntimeContractVersions.PublicHealthV1`, causing contract drift across runtime hosts.
- **Fix:** Add a project reference to `YO4X.Runtime.Contracts` in `YO4X.StrategyHost.csproj` and use `PublicRuntimeHealth` with `RuntimeContractVersions.PublicHealthV1`.

## Referrals
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` — Verify whether coordinator cancels in-flight evaluation tasks and releases host capacity when cancellation tokens are triggered during lease expiration.
- `src/Modules/RuntimeOperations/YO4X.RuntimeOperations/RuntimeReadinessEvaluator.cs` — Check if readiness evaluator handles partial evidence arrays when strategy host evidence is missing during supervisor restart.

## Coverage gaps
- `src/Runtime/YO4X.StrategyHost/StrategyExecutionCoordinator.cs:22` — No unit test tests the branch where `strategy.Handle(...)` throws an unhandled exception to verify whether the exception escapes or is bounded.
- `src/Runtime/YO4X.Supervisor/SupervisorUserOperationProtocolRegistration.cs:31-36` — No test exercises the invalid non-boolean configuration parse failure branch in supervisor host startup tests.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 131.4s | 270872 tok | id=7475cc8c-eb6e-4ef4-82c2-48a96e9818ab
