---
agent_id: I15
lane: sweep-nullability
scope:
  - src/Apps/YO4X.ControlPlane.Workers
  - src/Runtime/YO4X.Mql5.CodeGen
  - src/Runtime/YO4X.Mql5.Runtime
  - src/Runtime/YO4X.Mql5.Engine
  - src/Tools/YO4X.Backtest.Runner
  - src/Tools/YO4X.LiveBots
  - src/Tools/YO4X.StrategyInputProjection
  - src/Infrastructure/YO4X.ControlPlane.Postgres
  - src/Infrastructure/YO4X.Admin.Postgres
  - src/Infrastructure/YO4X.Trading.Postgres
  - src/Modules/Risk/YO4X.Risk
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance
  - src/Apps/YO4X.Conversion.Worker
  - src/Apps/YO4X.ControlPlane.Api
  - src/Apps/YO4X.Admin.Bff
  - src/Apps/YO4X.EmergencySafety.Api
  - src/Apps/YO4X.SecretIngestion.Api
  - src/Apps/YO4X.DevelopmentIdentity
  - src/Application/YO4X.Trading.Application
  - src/Application/YO4X.Runtime.Application
  - src/BuildingBlocks/YO4X.Api
  - src/BuildingBlocks/YO4X.BuildingBlocks
  - src/Runtime/YO4X.Trading.ProcessIsolation
  - src/Frontend/YO4X.Web
status: COMPLETE
generated: 2026-08-29T11:45:00Z
counts: { P0: 0, P1: 3, P2: 1, P3: 0 }
---

# I15 — sweep-nullability

## Scope audited

A cross-cutting nullability sweep was conducted across the entire codebase, reviewing project configuration (`<Nullable>enable</Nullable>`), deserialization boundaries, database reader mappings, null-forgiving operators (`!`), and transpiler code emission. The following files were reviewed in detail:

- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` (3831 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs` (1170 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs` (421 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1817 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs` (534 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs` (143 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs` (470 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ZeroedInstance.cs` (48 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs` (228 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs` (597 lines)
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs` (320 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs` (373 lines)
- `src/Tools/YO4X.Backtest.Runner/Program.cs` (680 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Tools/YO4X.StrategyInputProjection/Mql5InputProjection.cs` (390 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresMutationSupport.cs` (231 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs` (470 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminIdempotency.cs` (99 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminStorageValues.cs` (199 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs` (1158 lines)
- `src/Modules/Risk/YO4X.Risk/NumericRiskEvaluation.cs` (713 lines)
- `src/Modules/Risk/YO4X.Risk/NumericRiskPolicy.cs` (488 lines)
- `src/Modules/Risk/YO4X.Risk/EffectiveNumericRiskPolicy.cs` (370 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs` (1337 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs` (668 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs` (2695 lines)
- `src/Apps/YO4X.Conversion.Worker/PostgresMql5CorpusStore.cs` (1044 lines)
- `src/Apps/YO4X.Conversion.Worker/ConversionInventoryCommand.cs` (404 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` (1272 lines)
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlanePostgresRegistration.cs` (400 lines)
- `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs` (505 lines)
- `src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs` (356 lines)
- `src/Apps/YO4X.SecretIngestion.Api/SecretBodyReader.cs` (74 lines)
- `src/Apps/YO4X.DevelopmentIdentity/Controllers/AuthorizationController.cs` (134 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandReconciliationValidator.cs` (447 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventEvidence.cs` (1357 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` (465 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventIntakeContracts.cs` (367 lines)
- `src/BuildingBlocks/YO4X.Api/ClaimReader.cs` (31 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs` (64 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContractValidator.cs` (438 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerLaunchManifest.cs` (341 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs` (302 lines)
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines)

## Verdict

C# nullable reference types are enabled globally via `Directory.Build.props` without `#nullable disable` overrides, and public boundary decoders in `YO4X.Api`, `YO4X.BuildingBlocks`, and `YO4X.Web` enforce rigorous schema validation. However, specific nullability defects were uncovered where nullable database columns and null-forgiving initializations bypass type safety: worker proof readers crash with `InvalidOperationException` when reconciling operations with null dispatch message IDs; transpiled MQL5 module types leave ambient runtime/owner references as uninitialized `null!` inside arrays and struct instances; `ZeroMemory` sets strings to `null!` rather than empty strings; and the backtest runner silently coerces nullable schema columns to empty strings resulting in broken file resolution paths.

## Findings

### [P1] Unchecked `DispatchMessageId!.Value` in worker proof readers crashes reconciliation loop
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs:2536`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          AddTargetParameters(command, operation);
          command.Parameters.AddWithValue("dispatch_message_id", NpgsqlDbType.Uuid, operation.DispatchMessageId!.Value);
          await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
              .ConfigureAwait(false);
  ```
  *(and identically in `PostgresUserOperationWorkStore.cs:2692`)*:
  ```csharp
          command.Parameters.AddWithValue(
              "dispatch_message_id",
              NpgsqlDbType.Uuid,
              operation.DispatchMessageId!.Value);
  ```
- **Failure:** In `PersistedOperation`, `DispatchMessageId` is declared as nullable `Guid?`, representing the nullable column `control.user_operations.dispatch_message_id`. When `ProcessClaimedOpenOperationAsync` processes operations claimed via `ClaimOpenAsync` in `unknown` state (or `propagating`/`reconciling` where dispatch failed before a dispatch message was recorded), `operation.DispatchMessageId` is `null`. While `ReadDispatchStateAsync` (line 2456) safely checks `if (operation.DispatchMessageId is null) return null;`, `ReadDeploymentProofAsync` (line 2536) and `ReadBrokerProofAsync` (line 2692) unconditionally dereference `operation.DispatchMessageId!.Value`. This throws an unhandled `InvalidOperationException: Nullable object must have a value.` and crashes worker reconciliation.
- **Fix:** Add `if (operation.DispatchMessageId is null) { return null; }` at the beginning of both `ReadDeploymentProofAsync` and `ReadBrokerProofAsync` before preparing command parameters.

### [P1] Transpiled module types leave `_runtime` and `__owner` references as `null!` in arrays and struct locals
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:455-459`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              _writer.Line(
                  "internal IMql5Runtime " + Mql5RuntimeContract.RuntimeFieldName
                  + (isStruct ? ";" : " = null!;"));
              _writer.Line(
                  "internal " + StrategyTypeName + " " + OwnerFieldName
                  + (isStruct ? ";" : " = null!;"));
  ```
- **Failure:** Emitted MQL5 module types (structs and classes) declare `internal IMql5Runtime _runtime = null!;` and `internal StrategyType __owner = null!;` to route built-in function calls (`MathAbs`, `SymbolInfoDouble`) and global variable access back to the strategy. The transpiler emits object initializers (`{ _runtime = _runtime, __owner = this }`) only for single scalar constructions. When a strategy allocates an array of module types (e.g. `CTrade trades[5];` or `MyStruct matrix[3][3];` via `ArrayCreation` / `NewArray2<T>`), or declares an unassigned struct local or field, every element/instance is left with `_runtime == null` and `__owner == null`. When the strategy subsequently invokes any method on an array element (e.g. `trades[0].PositionOpen(...)`), dereferencing `_runtime` or `__owner` throws a runtime `NullReferenceException`.
- **Fix:** In array allocation helpers (`ArrayCreation`, `NewArray2`, `NewArray3`) and struct initialization routines, populate each element's `_runtime` and `__owner` fields upon instantiation.

### [P1] `ZeroMemory` sets `string` variables to `null!` causing `NullReferenceException` in subsequent string operations
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs:258-261`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (typeof(T).IsValueType || typeof(T) == typeof(string))
          {
              variable = default!;
              return;
          }
  ```
- **Failure:** In MQL5, strings are value types that initialize and reset to empty string (`""`). Calling `ZeroMemory(myString)` in MQL5 clears the string. In `Mql5Runtime.Terminal.cs`, `typeof(T) == typeof(string)` executes `variable = default!`, assigning `null!` to the string variable. Subsequent operations (e.g. `StringLen(myString)` or string concatenation methods) dereference `myString` and throw a `NullReferenceException`.
- **Fix:** Explicitly handle `string` in `ZeroMemory<T>` by setting `variable = (T)(object)string.Empty;` when `typeof(T) == typeof(string)`.

### [P2] Backtest runner coerces nullable `symbol` and `timeframe` columns to `string.Empty` without input validation
- **Where:** `src/Tools/YO4X.Backtest.Runner/Program.cs:174-176`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
              reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
              reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
  ```
- **Failure:** Migration `006_strategy_inputs_and_backtests.sql` made columns `symbol` and `timeframe` nullable in `simulation.backtests`. When a backtest row contains a `NULL` symbol or timeframe, `ReadNextAsync` sets `request.Symbol` and `request.Timeframe` to `string.Empty`. In `Execute()`, line 193 constructs `Path.Combine(dataRoot, server, request.Symbol, request.Timeframe + ".csv")`, resulting in malformed paths such as `<dataRoot>\<server>\.csv` and returning a misleading `"Market data file is missing: ..."` refusal instead of validating required parameters.
- **Fix:** Check whether `request.Symbol` or `request.Timeframe` are null/empty in `Execute()` and return an explicit refusal stating that required symbol and timeframe parameters are missing.

## Referrals

None.

## Coverage gaps

- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs:2536, 2692`: Untested reconciliation path for claimed user operations in `unknown` state where `dispatch_message_id` is `NULL`.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:455`: Untested execution of methods on elements of multi-dimensional arrays of translated MQL5 structs/classes calling runtime built-ins.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs:258`: Untested zeroing of `string` variables via `ZeroMemory` followed by standard MQL5 string built-in invocations.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 429.4s | 800168 tok | id=e65b8381-0f0b-43e7-92a4-5931d0abc20e
