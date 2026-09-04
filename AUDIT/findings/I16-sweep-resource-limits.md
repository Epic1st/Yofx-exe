---
agent_id: I16
lane: sweep-resource-limits
scope: whole tree (cross-cutting sweep for missing resource bounds, unbounded queries, memory accumulation, and missing limits)
status: complete
generated: 2026-08-29T11:36:30Z
counts:
  p0: 0
  p1: 0
  p2: 3
  p3: 2
  total: 5
---

# Audit Report: Sweep of Resource Limits and Unbounded Operations (Lane I16)

## Scope audited

A repository-wide, cross-cutting sweep was conducted across the codebase to identify missing resource bounds, uncapped in-memory collections, unbounded database reads, lack of pagination, and memory pressure vulnerabilities. The audit covered:

1. **Ingestion & Archive Intake**:
   - `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` and `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilePackageDossierPlanner.cs`: Archive entry counts, compression ratios, total uncompressed size, non-canonical file counts, and dependency depth ceilings.
2. **Frontend Read Models & Projections**:
   - `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs`: Query limits, page size bounds, and memory limits across catalogs, backtests, bot lists, journals, reviews, and cloud runners.
   - `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneReads.cs` & `PostgresBrokerAccountDiscoveryReads.cs`: Bounded pagination and limits on active sessions, audit events, broker accounts, and registration options.
   - `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs`: Listing and pagination for strategy source corpora and compatibility matrices.
3. **Control Plane, Admin, and Emergency Safety Subsystems**:
   - `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs`, `AdminPostgresApplication.Reads.cs`, `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs`, and `src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs`: Database queries, paging, and target list materialization for command containment and administrative reads.
4. **MQL5 Language Front-End & Execution Runtime**:
   - `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs`: Recursion and nesting depth limits (`MaximumNestingDepth = 512`), diagnostic collection bounds (`MaximumDiagnostics = 500`).
   - `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs`: Structural lowering depth ceilings (`MaximumDepth = 192`).
   - `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs` & `Mql5Log.cs`: Global variable storage, log sinks (`Mql5LogRecorder` capacity limit = 4096), and chart recording stores.
   - `src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs`: In-memory chart object stores, object registries, and point coordinate mutations.
   - `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs` & `src/Runtime/YO4X.Mql5.Engine/Indicators/`: In-memory indicator handle caching, indicator buffer allocation, and historical bar retention during backtests and simulations.
5. **Worker Hosts, Background Schedulers & Import Tooling**:
   - `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` & `PostgresDeploymentProjectionStore.cs`: Polling loops, batch sizes (`InvocationTimeoutBatchSizePerTenant`, `OperationBatchSizePerTenant`, `DeploymentBatchSizePerTenant`), and task timeouts.
   - `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs`: Concurrency bounding (`ParallelOptions.MaxDegreeOfParallelism = 4`, `SocketsHttpHandler.MaxConnectionsPerServer = 4`).
   - `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs` & `LeanTickZipWriter.cs`: Memory materialization during high-frequency tick data processing.

---

## Verdict

The codebase demonstrates strong resource bounding across core ingestion pipelines and primary public API read models. Specifically:
- Quarantine intake in `Mql5QuarantineIntakeJob.cs` strictly enforces archive entry counts (`MaximumArchiveEntryCount = 128`), individual and total decompressed size limits (`MaximumArchiveByteCount = 64 MB`), non-canonical file counts (`MaximumNonCanonicalFileCount = 256`), and compression ratios (`MaximumArchiveCompressionRatio = 100`) to prevent decompression bomb attacks.
- Frontend projection queries in `PostgresFrontendProjections.cs` consistently enforce pagination caps (`StrategyInputLimit = 1_000`, `BacktestEquityPointLimit = 2_001`, `BotListLimit = 200`, `JournalLimitMaximum = 200`).
- The MQL5 recursive-descent parser and IR lowering passes enforce explicit depth limits (`MaximumNestingDepth = 512`, `MaximumDepth = 192`) and diagnostic limits (`MaximumDiagnostics = 500`), preventing stack exhaustion on adversarial inputs.
- Concurrency in worker schedulers and external scrapers is bounded via batch sizes and fixed degrees of parallelism.

However, the sweep identified several unbounded collections and queries:
1. `Mql5ChartObjectStore` retains chart visual objects with no eviction or maximum object cap, and performs unbounded list allocations on arbitrary point indices.
2. `Mql5MarketContext` caches indicator instances and handles with no size cap, eviction policy, or handle release mechanism when EAs construct indicators dynamically across ticks.
3. `AdminReadRepository.GetTargetsAsync` queries and loads all command targets into memory with no `LIMIT` or pagination.
4. `Mt5TickExportReader` and `LeanTickZipWriter` fully materialize multi-gigabyte tick exports into memory arrays and string builders.
5. `PostgresStrategyCompatibilityReads.GetStrategySourceCorporaAsync` retrieves all strategy source corpora for a tenant without pagination or a `LIMIT` clause.

---

## Findings

### FINDING-I16-01: Unbounded Chart Object Accumulation and Anchor Allocation in `Mql5ChartObjectStore`
- **Severity**: P2
- **File**: `src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs:64-78, 108-111`
- **Component**: `YO4X.Mql5.Runtime.Mql5ChartObjectStore`
- **Description**: 
  `Mql5ChartObjectStore` maintains in-memory dictionary registries (`charts` and per-chart `state.Objects` / `state.Ordered`) for objects created via `ObjectCreate`. The store has no maximum object count ceiling and no eviction mechanism. If an EA creates graphical objects (such as arrows, signal markers, or trendlines) with timestamp-derived or unique names on every tick or candle (e.g., `ObjectCreate(0, "sig_" + IntegerToString(TimeCurrent()), OBJ_ARROW, ...)`), the object dictionary and anchor lists grow indefinitely over the lifecycle of a multi-million-tick backtest.
  Additionally, `Move` at lines 108–111 attempts to fill anchors up to `pointIndex` using `while (found.Anchors.Count <= pointIndex) found.Anchors.Add((0, 0));`. An attacker-controlled or erroneous `pointIndex` (e.g., `10_000_000`) causes an unbounded allocation loop allocating millions of tuples in memory.
- **Evidence**:
  `src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs:64-78`:
  ```csharp
      internal bool Create(long chartId, string name, int type, int subWindow, IReadOnlyList<(long Time, double Price)> anchors)
      {
          Record();
          ChartState state = State(chartId);
          if (state.Objects.ContainsKey(name))
          {
              return false;
          }

          Mql5ChartObject created = new(name, type, subWindow);
          created.Anchors.AddRange(anchors);
          state.Objects[name] = created;
          state.Ordered.Add(created);
          return true;
      }
  ```
  `src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs:108-111`:
  ```csharp
          while (found.Anchors.Count <= pointIndex)
          {
              found.Anchors.Add((0, 0));
          }
  ```
- **Scenario**: 
  A backtest running against 5,000,000 ticks where an EA logs 1 arrow object per tick creates 5,000,000 `Mql5ChartObject` instances and corresponding dictionary entries, consuming several gigabytes of heap memory without eviction. Calling `ObjectMove(0, "arrow", 5000000, ...)` triggers a loop allocating 5,000,000 anchor tuples immediately.
- **Fix**: 
  1. Enforce a maximum object limit per chart (e.g., `const int MaxChartObjects = 4096`) in `Create` and reject further creations with `Mql5ErrorCodes.ChartObjectCannotCreate` or evict the oldest object in `state.Ordered`.
  2. Validate `pointIndex` against a bounded ceiling (e.g., `if (pointIndex < 0 || pointIndex >= MaxChartObjectPoints) return false;`, where `MaxChartObjectPoints = 16`).

---

### FINDING-I16-02: Unbounded Indicator Instance and Handle Allocation in `Mql5MarketContext`
- **Severity**: P2
- **File**: `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs:16-18, 62-69, 248-274`
- **Component**: `YO4X.Mql5.Engine.Context.Mql5MarketContext`
- **Description**: 
  In `Mql5MarketContext`, indicator handles and indicator instances are stored in `indicatorHandles` (`Dictionary<string, int>`) and `indicators` (`List<IMql5Indicator>`). When `IndicatorHandle(name, arguments)` is invoked, it checks the cache key. If the key is absent, it instantiates a new indicator via `Mql5IndicatorFactory.Create`, backfills the entire historical bar collection into it (`foreach (Mql5Bar bar in bars) indicator.Append(bar)`), and registers it into the `indicators` list.
  There is no maximum indicator handle cap, no LRU/LFU eviction policy, and `IndicatorRelease` does not remove indicator instances from `indicators` or trim the `indicatorHandles` dictionary. An EA that constructs indicator calls with dynamic parameters computed at runtime (e.g., dynamic period based on volatility or spread) creates a new indicator instance on every tick, each with its own internal `List<double>[]` buffers spanning the full bar history.
- **Evidence**:
  `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs:248-274`:
  ```csharp
      public int IndicatorHandle(string name, params object[] arguments)
      {
          string key = IndicatorKey(name, arguments);
          if (indicatorHandles.TryGetValue(key, out int existing))
          {
              return existing;
          }

          IMql5Indicator indicator = Mql5IndicatorFactory.Create(name, arguments);
          foreach (Mql5Bar bar in bars)
          {
              indicator.Append(bar);
          }

          int handle = indicators.Count;
          indicators.Add(indicator);
          indicatorHandles[key] = handle;
          return handle;
      }
  ```
- **Scenario**: 
  An EA executes `iMA(_Symbol, _Period, dynamicPeriod, ...)` where `dynamicPeriod` varies across 10,000 distinct values during a simulation. `Mql5MarketContext` creates 10,000 `Mql5MovingAverageIndicator` instances. If history contains 100,000 bars, each indicator allocates 100,000 `double` entries (800 KB each), resulting in ~8 GB of memory allocation and O(N * M) tick processing overhead on every subsequent `Tick()`.
- **Fix**: 
  1. Introduce a strict ceiling on the number of active indicators per runtime instance (e.g., `const int MaximumIndicatorHandles = 256`).
  2. Implement proper indicator handle reclamation in `IndicatorRelease(int handle)` that unregisters the indicator, drops cached buffers, and frees the handle slot.

---

### FINDING-I16-03: Unpaged Database Query in `AdminReadRepository.GetTargetsAsync`
- **Severity**: P2
- **File**: `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs:222-263`
- **Component**: `YO4X.Admin.Postgres.AdminReadRepository`
- **Description**: 
  `AdminReadRepository.GetTargetsAsync` executes a database query against `control.command_targets` without a `LIMIT` clause or keyset pagination. This repository method is invoked by `AdminPostgresApplication.GetCommandTargetsAsync` and exposed directly via HTTP endpoints in both `AdminRoutes` (`GET /admin/v1/commands/{commandId}/targets`) and `EmergencyRoutes` (`GET /emergency/v1/restrictive-commands/{commandId}/targets`).
  When an operator executes a broad restrictive command or emergency containment order across an entire tenant (e.g., emergency stop or quarantine affecting all bot deployments, worker assignments, and accounts for a tenant with thousands of entities), `control.command_targets` may hold thousands of rows for that `command_id`. Querying this endpoint fetches and materializes the entire table slice into a single `List<CommandTargetView>` in memory and emits it as a single JSON response payload.
- **Evidence**:
  `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs:222-263`:
  ```csharp
      public static async Task<IReadOnlyList<CommandTargetView>> GetTargetsAsync(
          TenantPostgresTransaction transaction,
          Guid commandId,
          CancellationToken cancellationToken)
      {
          await using NpgsqlCommand command = transaction.CreateCommand(
              """
              select
                  id,
                  resource_id,
                  resource_type,
                  resource_version,
                  required_proof,
                  required,
                  worker_id,
                  generation,
                  state,
                  attempts,
                  dispatched_at,
                  delivered_at,
                  acknowledged_at,
                  applied_at,
                  reconciled_at,
                  observed_result,
                  broker_evidence_reference,
                  last_error_code,
                  created_at,
                  updated_at
              from control.command_targets
              where tenant_id = @tenant_id
                and command_id = @command_id
              order by id
              """);
          AddUuid(command, "tenant_id", transaction.Context.TenantId);
          AddUuid(command, "command_id", commandId);
          var results = new List<CommandTargetView>();
          await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
              .ConfigureAwait(false);
          while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
          {
              results.Add(new CommandTargetView(
                  ...
  ```
- **Scenario**: 
  An emergency containment command generates 15,000 target entries for a multi-deployment tenant. An operator viewing the admin portal or automated incident monitor polling the target endpoint causes `GetTargetsAsync` to execute an unpaged query loading 15,000 `CommandTargetView` objects, generating high memory allocation and serialization overhead.
- **Fix**: 
  Add pagination parameters (`limit`, `after_id` / cursor) to `GetTargetsAsync`, clamped to a safe maximum (e.g., `Math.Clamp(limit ?? 50, 1, 100)`), consistent with `GetApprovalsAsync` and `GetApprovalPageAsync`.

---

### FINDING-I16-04: Full In-Memory Materialization of Large Tick Archives in `Mt5TickExportReader` and `LeanTickZipWriter`
- **Severity**: P3
- **File**: `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs:77-137` and `src/Tools/YO4X.MarketData.Mt5Import/LeanTickZipWriter.cs:51-66`
- **Component**: `YO4X.MarketData.Mt5Import`
- **Description**: 
  `Mt5TickExportReader.Read` reads exported MT5 tick files by splitting each line into individual string arrays and appending every tick into `List<Mt5QuoteRow> rows` without batching or streaming. Real-world MT5 tick exports for multi-month or multi-year high-frequency quote data contain tens of millions of rows (several gigabytes on disk). Retaining all rows in a single `List<Mt5QuoteRow>` consumes gigabytes of heap memory and can lead to `OutOfMemoryException`.
  Furthermore, `LeanTickZipWriter.RenderDay` converts all ticks for a day by allocating a single large `StringBuilder`, generating a single large string via `builder.ToString()`, and returning a contiguous `byte[] payload = Encoding.UTF8.GetBytes(...)` in memory before writing to the zip stream.
- **Evidence**:
  `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs:77-85, 136`:
  ```csharp
          var rows = new List<Mt5QuoteRow>();
          var rejections = new Dictionary<string, Mt5RejectionTally>(StringComparer.Ordinal);
          long lineNumber = 1;
          long dataLineCount = 0;
          long sequence = 0;

          while (reader.ReadLine() is { } line)
          {
              ...
              rows.Add(new Mt5QuoteRow(sequence, lineNumber, timestampUtc, bid, ask, flagsText));
          }
  ```
  `src/Tools/YO4X.MarketData.Mt5Import/LeanTickZipWriter.cs:51-66`:
  ```csharp
      internal static byte[] RenderDay(IReadOnlyList<Mt5QuoteRow> ascendingTicks)
      {
          var builder = new StringBuilder();
          foreach (Mt5QuoteRow tick in ascendingTicks)
          {
              long milliseconds = tick.TimestampUtc.TimeOfDay.Ticks / TimeSpan.TicksPerMillisecond;
              builder.Append(milliseconds.ToString(CultureInfo.InvariantCulture));
              builder.Append(',');
              builder.Append(tick.Bid.ToString(CultureInfo.InvariantCulture));
              builder.Append(',');
              builder.Append(tick.Ask.ToString(CultureInfo.InvariantCulture));
              builder.Append('\n');
          }

          return Encoding.UTF8.GetBytes(builder.ToString());
      }
  ```
- **Scenario**: 
  Importing an MT5 tick export file containing 30,000,000 tick rows (~1.5 GB file on disk) allocates 30 million `Mt5QuoteRow` struct instances, individual string flag allocations, and temporary `string[]` field arrays from `line.Split()`, exceeding process memory limits and triggering GC thrashing or `OutOfMemoryException`.
- **Fix**: 
  Stream tick data using `IEnumerable<Mt5QuoteRow>` / `IAsyncEnumerable<Mt5QuoteRow>` with a day-partitioned streaming pipeline. Write formatted ticks directly to the `ZipArchiveEntry` stream via `StreamWriter` without allocating intermediate `StringBuilder` strings and large `byte[]` arrays in memory.

---

### FINDING-I16-05: Missing Pagination and LIMIT on `GetStrategySourceCorporaAsync`
- **Severity**: P3
- **File**: `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs:28-59`
- **Component**: `YO4X.ControlPlane.Postgres.PostgresControlPlaneApplication`
- **Description**: 
  `GetStrategySourceCorporaAsync` queries `governance.strategy_source_corpora` and joins `governance.strategy_source_files` for a given tenant and user without a `LIMIT` clause or pagination. Over time, as a user imports numerous MQL5 source packages and revisions, the query results grow unbounded, materializing all historical corpus summaries in a single list.
- **Evidence**:
  `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs:28-48`:
  ```csharp
              await using NpgsqlCommand command = transaction.CreateCommand(
                  """
                  select
                      corpus.id,
                      corpus.source_label,
                      corpus.file_count,
                      corpus.total_bytes,
                      corpus.created_at,
                      count(source_file.id)
                  from governance.strategy_source_corpora as corpus
                  left join governance.strategy_source_files as source_file
                    on source_file.tenant_id = corpus.tenant_id
                   and source_file.corpus_id = corpus.id
                   and source_file.user_id = corpus.user_id
                  where corpus.tenant_id = @tenant_id
                    and corpus.user_id = @user_id
                    and corpus.state = 'static_analyzed'
                  group by corpus.id, corpus.source_label, corpus.file_count,
                           corpus.total_bytes, corpus.created_at
                  order by corpus.created_at desc, corpus.id desc
                  """);
  ```
- **Scenario**: 
  A user or automated CI/CD pipeline imports 1,500 distinct strategy corpora over time. Calling `GetStrategySourceCorporaAsync` scans all 1,500 rows, executes an aggregation join across all associated source files, and materializes all 1,500 summaries into memory on every request.
- **Fix**: 
  Add a `limit @limit` parameter (e.g., default 50, maximum 100) and cursor-based pagination using `corpus.created_at` and `corpus.id`.

---

## Referrals

None.

---

## Coverage gaps

1. **Simulated Broker Order Book & Trade Event Retention**: `Mql5SimulatedBroker` retains closed positions and order transactions in memory for backtests. Long-running grid/martingale strategy runs that execute hundreds of thousands of trades were analyzed structurally; verifying exact memory growth per million trades requires executing dedicated end-to-end backtesting performance benchmarks with live telemetry.
2. **PostgreSQL Statement Timeouts**: Individual command execution timeouts (`NpgsqlCommand.CommandTimeout`) rely on connection string defaults rather than per-query limits across all transaction boundaries. Ensuring slow database queries abort predictably under heavy load requires database configuration auditing.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 430.8s | 563813 tok | id=b47acd9d-24a8-49ab-9db8-5f88728d68f1
